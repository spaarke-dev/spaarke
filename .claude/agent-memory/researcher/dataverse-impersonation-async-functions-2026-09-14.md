---
name: dataverse-impersonation-async-functions-2026-09-14
description: May an unattended Azure Function (stamp UAMI, app-only) impersonate a Dataverse user for BFF-initiated async work? MS docs position, trust model, audit/licensing, async precedent, and Spaarke's Service Bus SAS gap (input to ADR-052 identity row).
metadata:
  type: project
---

# Dataverse impersonation for async / Azure Functions work — 2026-09-14

**Question**: Should ADR-052's "Function MUST NOT impersonate a Dataverse caller" ban stay, or is conditional impersonation (caller id taken from a BFF-authored job) the correct approach?

**Findings (MS Learn, verified 2026-09-14)**
- Impersonation is a first-class, documented pattern for *services*, not just requests: "Various clients and services can use impersonation…"; the Web API page names "a workflow or custom ISV solution" (impersonate-another-user, ms.date 2026-08-20; web-api page 2026-03-27). S2S page: an application user doing work for a user "can apply impersonation"; the S2S app "is responsible for controlling access to the data that it has access to" = the ONLY documented trust rule (the platform trusts whatever id the privileged caller sends).
- Headers: `CallerObjectId` = Entra oid (**Preferred**) · `MSCRMCallerID` = systemuserid (**Legacy**). SDK: ServiceClient `CallerId` (systemuserid) / `CallerAADObjectId` (oid). Effective rights = INTERSECTION. `prvActOnBehalfOfAnotherUser` must be assigned **directly — cannot be inherited through a team** "because of the sensitive nature of the privilege".
- Audit: `createdby`/`modifiedby` = impersonated user; `createdonbehalfby`/`modifiedonbehalfby` = actual caller; audit table `callinguserid` = "calling user in case of an impersonated call". Service-protection limits are per authenticated user; app users get the same limits (the page doesn't say which side of an impersonated call is counted).
- Async precedent: Dataverse's own async plug-ins default to "Calling User" (the initiating user) and can override via `InitiatingUserId`; the Service Bus integration posts a `RemoteExecutionContext` carrying UserId/InitiatingUserId to external listeners. So MS's own queued-work design carries the requesting user's identity across the async boundary.
- OBO for long-running work = MSAL `InitiateLongRunningProcessInWebApi`/`AcquireTokenInLongRunningProcess` (refresh token in distributed cache, `MsalUiRequiredException` when re-sign-in needed) — needs a confidential client + stored user refresh tokens; Spaarke forbids both (ADR-028 A1-A3, ADR-052 identity row).
- No MS page says impersonation is unsupported or discouraged for background work; no doc restricts impersonating unlicensed users (licensing is separate: multiplexing rules still require the end users to be licensed). Contacts can't be impersonated (not principals).

**Spaarke facts found**: live helper `Spaarke.Dataverse/DataverseImpersonation.cs` uses `MSCRMCallerID`+systemuserid (legacy header), not CallerObjectId+oid; fail-closed on Guid.Empty is in `RetrieveMultipleImpersonatedAsync`, NOT the helper. `JobContract` has only a loose `SubjectId` (user/resource) — no typed requesting-principal field. **`infrastructure/bicep/modules/service-bus.bicep` leaves local (SAS) auth ON and outputs a Send+Listen connection string**; `ServiceBusClientFactory` falls back to a connection string → "only the stamp can write the queue" is NOT true today, and a forged message could name any user.

**Sources**: learn.microsoft.com/power-apps/developer/data-platform/{impersonate-another-user, webapi/impersonate-another-user-web-api, impersonate-a-user, build-web-applications-server-server-s2s-authentication, azure-integration, api-limits, reference/entities/audit}; power-platform/admin/miscellaneous-privileges; dotnet/api ServiceClient.CallerAADObjectId; entra/msal/dotnet/.../on-behalf-of-flow (long-running OBO); azure/service-bus-messaging/service-bus-authentication-and-authorization (Entra recommended, disable local auth).

**Open questions**: Which user service-protection limits are charged to under impersonation (undocumented). Whether revocation between enqueue and execution should be re-checked (disabled user → Dataverse rejects? not documented; test it).

Related: [[dataverse-record-access-security-2026-07-16]], [[dataverse-record-restriction-secure-project-2026-08-20]], [[background-work-hosting-best-practice-2026-09-12]]
